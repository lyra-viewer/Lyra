using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Lyra.Common;

[TomlSerializable(typeof(TomlTable))]
internal partial class LyraTomlContext : TomlSerializerContext;