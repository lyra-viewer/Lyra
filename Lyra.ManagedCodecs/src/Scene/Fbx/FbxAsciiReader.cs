using System.Globalization;
using System.Text;

namespace Lyra.ManagedCodecs.Scene.Fbx;

/// <summary>
/// ASCII FBX, version 6 or 7: <c>Name: value, value {</c> nodes nested in braces. Only the nodes
/// of the texture graph are read; every other body, mesh arrays included, is skipped by matching
/// braces, without recursion. Embedded media is base64 and is decoded here.
/// </summary>
internal sealed class FbxAsciiReader
{
    private readonly byte[] _text;
    private readonly CancellationToken _ct;
    private readonly IssueLog _issues = new();
    private int _at;

    /// <summary>One value of a node: a string, a number, a bare word, or an array that was skipped.</summary>
    private readonly record struct Value(string? Text, long? Integer, bool IsString)
    {
        public string? Key => Integer?.ToString(CultureInfo.InvariantCulture) ?? Text;
    }

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private FbxAsciiReader(byte[] text, CancellationToken ct)
    {
        _text = text;
        _ct = ct;
    }

    /// <summary>Whether the bytes look like ASCII FBX: its comment header, or its first top-level node.</summary>
    public static bool Recognizes(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(Utf8Bom))
            head = head[Utf8Bom.Length..];

        var text = Encoding.ASCII.GetString(head).TrimStart(' ', '\t', '\r', '\n');
        return text.StartsWith("; FBX", StringComparison.Ordinal) || text.StartsWith("FBXHeaderExtension", StringComparison.Ordinal);
    }

    public static FbxDocument Read(byte[] text, CancellationToken ct) => new FbxAsciiReader(text, ct).ReadDocument();

    private FbxDocument ReadDocument()
    {
        string? creator = null;
        var objects = new List<FbxObject>();
        var connections = new List<FbxConnection>();

        while (NextNode(out var name))
        {
            _ct.ThrowIfCancellationRequested();

            var values = ReadValues();
            var hasBody = OpenBody();

            switch (name)
            {
                case "Creator":
                    creator ??= values.FirstOrDefault(value => value.IsString).Text;
                    break;

                case "FBXHeaderExtension" when hasBody:
                    ReadChildren(child =>
                    {
                        var childValues = ReadValues();
                        if (OpenBody())
                            SkipBody();

                        if (child == "Creator")
                            creator ??= FirstString(childValues);
                    });
                    continue;

                case "Objects" when hasBody:
                    ReadObjects(objects);
                    continue;

                case "Connections" when hasBody:
                    ReadConnections(connections);
                    continue;
            }

            if (hasBody)
                SkipBody();
        }

        return new FbxDocument(creator, objects, connections, _issues.ToList());
    }

    private void ReadObjects(List<FbxObject> objects)
    {
        ReadChildren(type =>
        {
            var values = ReadValues();
            var hasBody = OpenBody();

            if (!FbxDocument.IsGraphType(type))
            {
                if (hasBody)
                    SkipBody();

                return;
            }

            // FBX 7: id, "Type::Name", class. FBX 6: "Type::Name", class, and the name is the id.
            var key = values.Count > 0 ? values[0].Key : null;
            var rawName = values.Count > 0 && values[0].IsString ? values[0].Text : values.Skip(1).FirstOrDefault(value => value.IsString).Text;

            string? fileName = null, relative = null;
            FbxContent? content = null;

            if (hasBody)
            {
                ReadChildren(child =>
                {
                    switch (child)
                    {
                        case "FileName" or "Filename":
                            fileName ??= FirstString(ReadValues());
                            break;

                        case "RelativeFilename":
                            relative ??= FirstString(ReadValues());
                            break;

                        case "Content":
                            content ??= DecodeContent(ReadValues());
                            break;

                        default:
                            ReadValues();
                            break;
                    }

                    if (OpenBody())
                        SkipBody();
                });
            }

            if (key is null)
            {
                Issue($"A {type} near byte {_at} has no id.");
                return;
            }

            objects.Add(new FbxObject(key, type, FbxDocument.DisplayName(rawName ?? ""))
            {
                FileName = fileName,
                RelativeFilename = relative,
                Content = content
            });
        });
    }

    private void ReadConnections(List<FbxConnection> connections)
    {
        ReadChildren(name =>
        {
            var values = ReadValues();
            if (OpenBody())
                SkipBody();

            if (name is ("C" or "Connect") && values.Count >= 3 && values[0].Text is { } kind && values[1].Key is { } child && values[2].Key is { } parent)
                connections.Add(new FbxConnection(kind, child, parent, values.Count > 3 ? values[3].Text : null));
        });
    }

    /// <summary>The media, which may be split across several quoted strings; null when empty or not base64.</summary>
    private FbxContent? DecodeContent(List<Value> values)
    {
        var base64 = string.Concat(values.Where(value => value.IsString).Select(value => value.Text));
        if (base64.Length == 0)
            return null;

        try
        {
            return new FbxContentBytes(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            Issue("A Video's embedded content is not valid base64.");
            return null;
        }
    }

    private static string? FirstString(List<Value> values) => values.FirstOrDefault(value => value.IsString).Text;
    
    private void ReadChildren(Action<string> read)
    {
        while (true)
        {
            _ct.ThrowIfCancellationRequested();
            SkipSpaceAndComments();

            if (_at >= _text.Length)
            {
                Issue("The file ends inside a node.");
                return;
            }

            if (_text[_at] == '}')
            {
                _at++;
                return;
            }

            if (!ReadName(out var name))
                continue;

            read(name);
        }
    }

    private bool NextNode(out string name)
    {
        while (true)
        {
            SkipSpaceAndComments();

            if (_at >= _text.Length)
            {
                name = "";
                return false;
            }

            if (_text[_at] == '}')
            {
                _at++;
                continue;
            }

            if (ReadName(out name))
                return true;
        }
    }
    
    private bool ReadName(out string name)
    {
        var start = _at;
        while (_at < _text.Length && _text[_at] is not ((byte)':' or (byte)'\n' or (byte)'{' or (byte)'}' or (byte)','))
            _at++;

        name = "";

        if (_at >= _text.Length)
            return false;

        switch (_text[_at])
        {
            case (byte)':':
                name = Encoding.ASCII.GetString(_text, start, _at - start).Trim();
                _at++;
                return true;

            case (byte)'}':
                return false;

            case (byte)'{':
                _at++;
                SkipBody();
                return false;

            default:
                SkipLine();
                return false;
        }
    }
    
    private List<Value> ReadValues()
    {
        var values = new List<Value>();

        while (true)
        {
            SkipInlineSpace();
            if (_at >= _text.Length)
                return values;

            switch (_text[_at])
            {
                case (byte)'"':
                    values.Add(new Value(ReadQuoted(), null, true));
                    break;

                case (byte)'*':
                    SkipArray();
                    values.Add(default);
                    break;

                case (byte)',':
                    values.Add(default);
                    break;

                case (byte)'\n' or (byte)'\r' or (byte)'{' or (byte)'}' or (byte)';':
                    return values;

                default:
                    var token = ReadToken();
                    values.Add(long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer) ? new Value(token, integer, false) : new Value(token, null, false));
                    break;
            }

            SkipInlineSpace();
            if (_at >= _text.Length || _text[_at] != ',')
                return values;

            _at++;
            SkipSpaceAndComments();
        }
    }
    
    private bool OpenBody()
    {
        SkipInlineSpace();
        if (_at < _text.Length && _text[_at] == '{')
        {
            _at++;
            return true;
        }

        return false;
    }

    private void SkipBody()
    {
        var depth = 1;

        while (_at < _text.Length)
        {
            switch (_text[_at])
            {
                case (byte)'"':
                    SkipQuoted();
                    continue;

                case (byte)';':
                    SkipLine();
                    continue;

                case (byte)'{':
                    depth++;
                    break;

                case (byte)'}':
                    if (--depth == 0)
                    {
                        _at++;
                        return;
                    }

                    break;
            }

            _at++;

            if ((_at & 0xFFFFF) == 0)
                _ct.ThrowIfCancellationRequested();
        }

        Issue("The file ends inside a node.");
    }

    private void SkipArray()
    {
        _at++;
        while (_at < _text.Length && _text[_at] is >= (byte)'0' and <= (byte)'9')
            _at++;

        if (OpenBody())
            SkipBody();
    }

    private string ReadQuoted()
    {
        var start = ++_at;
        while (_at < _text.Length && _text[_at] != '"')
            _at++;

        var text = Encoding.UTF8.GetString(_text, start, _at - start).Replace("&quot;", "\"", StringComparison.Ordinal);

        if (_at < _text.Length)
            _at++;

        return text;
    }

    private void SkipQuoted()
    {
        _at++;
        while (_at < _text.Length && _text[_at] != '"')
            _at++;

        if (_at < _text.Length)
            _at++;
    }

    private string ReadToken()
    {
        var start = _at;
        while (_at < _text.Length && _text[_at] is not ((byte)',' or (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'{' or (byte)'}'))
            _at++;

        return Encoding.ASCII.GetString(_text, start, _at - start);
    }

    private void SkipInlineSpace()
    {
        while (_at < _text.Length && _text[_at] is (byte)' ' or (byte)'\t')
            _at++;
    }

    private void SkipSpaceAndComments()
    {
        while (_at < _text.Length)
        {
            var c = _text[_at];
            if (c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0xEF or 0xBB or 0xBF)
                _at++;
            else if (c == ';')
                SkipLine();
            else
                return;
        }
    }

    private void SkipLine()
    {
        while (_at < _text.Length && _text[_at] != '\n')
            _at++;
    }

    private void Issue(string text) => _issues.Add(text);
}