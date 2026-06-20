using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.FileLoader.Store;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.Button;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.Renderer.GUI.Sections;

// ============================================================================
//  DebugSection - sidebar collapsible with developer-facing diagnostics.
// ----------------------------------------------------------------------------
//  Uses the same two-column key/value layout as ExifSection / FormatSection,
//  but with a private key column (not shared via KeyColumnRegistry). Debug
//  keys are static and computed once at construction time.
//
//  Two kinds of content:
//
//    1. Input-driven rows (updated imperatively from UIManager input
//       handlers via SetPointer / SetHit / SetAction):
//         Pointer / Hit / Action
//
//    2. State-driven rows (refreshed from UIState):
//         State / Decoder / Time Est / Time Complete
//         Drop / Drop Enqueued / Drop Files / Drop Supported
//
//  Hidden by default. Rows may clip horizontally inside narrow sidebars.
//  The sidebar is user-resizable, so this is acceptable for a debug section.
// ============================================================================
public sealed class DebugSection : IUISection
{
    private readonly Collapsible _collapsible;

    // Value labels - the only ones that change at runtime.
    private readonly Label _pointerValue;
    private readonly Label _hitValue;
    private readonly Label _actionValue;

    private readonly HStack _stateRow;
    private readonly Label _stateValue;

    private readonly HStack _decoderRow;
    private readonly Label _decoderValue;

    private readonly HStack _timeEstRow;
    private readonly Label _timeEstValue;

    private readonly HStack _timeCompleteRow;
    private readonly Label _timeCompleteValue;

    private readonly HStack _dropStatusRow;
    private readonly Label _dropStatusValue;

    private readonly Label _dropEnqueuedValue;
    private readonly Label _dropFilesValue;
    private readonly Label _dropSupportedValue;

    // Current FileRecord rows (size / content hash / pHash populate during a duplicate scan).
    private readonly HStack _recordNameRow;
    private readonly Label _recordNameValue;
    private readonly HStack _recordDirRow;
    private readonly Label _recordDirValue;
    private readonly HStack _recordSizeRow;
    private readonly Label _recordSizeValue;
    private readonly HStack _recordContentHashRow;
    private readonly Label _recordContentHashValue;
    private readonly HStack _recordPHashRow;
    private readonly Label _recordPHashValue;
    private readonly HStack _recordGroupRow;
    private readonly Label _recordGroupValue;

    public Collapsible Collapsible => _collapsible;

    public IComponent Root => _collapsible;

    public DebugSection()
    {
        // Key labels are collected during row construction so we can
        // compute the shared column width once all rows exist.
        var keyLabels = new List<Label>();

        // Input-driven rows
        var pointerRow = BuildRow(keyLabels, "Pointer", "-", out _pointerValue);
        pointerRow.Padding = new Padding(0, 4, 0, 0);
        var hitRow = BuildRow(keyLabels, "Hit", "-", out _hitValue);
        var actionRow = BuildRow(keyLabels, "Action", "-", out _actionValue);

        // Composite state rows
        _stateRow = BuildRow(keyLabels, "State", "", out _stateValue);
        _decoderRow = BuildRow(keyLabels, "Decoder", "", out _decoderValue);
        _timeEstRow = BuildRow(keyLabels, "Time Est (ms)", "", out _timeEstValue);
        _timeCompleteRow = BuildRow(keyLabels, "Time Complete (ms)", "", out _timeCompleteValue);

        // Drag & drop rows
        _dropStatusRow = BuildRow(keyLabels, "Drop", "", out _dropStatusValue);
        var dropEnqueuedRow = BuildRow(keyLabels, "Drop Enqueued", "0", out _dropEnqueuedValue);
        var dropFilesRow = BuildRow(keyLabels, "Drop Files", "0", out _dropFilesValue);
        var dropSupportedRow = BuildRow(keyLabels, "Drop Supported", "0", out _dropSupportedValue);

        // Current FileRecord rows
        _recordNameRow = BuildRow(keyLabels, "Rec Name", "-", out _recordNameValue);
        _recordDirRow = BuildRow(keyLabels, "Rec Dir", "-", out _recordDirValue);
        _recordSizeRow = BuildRow(keyLabels, "Rec Size", "-", out _recordSizeValue);
        _recordContentHashRow = BuildRow(keyLabels, "Rec Hash", "-", out _recordContentHashValue);
        _recordPHashRow = BuildRow(keyLabels, "Rec pHash", "-", out _recordPHashValue);
        _recordGroupRow = BuildRow(keyLabels, "Rec Group", "-", out _recordGroupValue);

        // Compute the shared key-column width once and apply to every key label.
        var maxKeyWidth = 0f;
        foreach (var label in keyLabels)
            maxKeyWidth = Math.Max(maxKeyWidth, Label.MeasureTextWidth(label.Text));

        foreach (var label in keyLabels)
            label.Width = maxKeyWidth;

        _collapsible = new Collapsible("DEBUG")
        {
            HorizontalSize = SizeMode.Expand,
            IsExpanded = false
        };
        _collapsible.AddComponents(
            pointerRow,
            hitRow,
            actionRow,
            Spacer(),
            _stateRow,
            _decoderRow,
            _timeEstRow,
            _timeCompleteRow,
            Spacer(),
            _dropStatusRow,
            dropEnqueuedRow,
            dropFilesRow,
            dropSupportedRow,
            Spacer(),
            _recordNameRow,
            _recordDirRow,
            _recordSizeRow,
            _recordContentHashRow,
            _recordPHashRow,
            _recordGroupRow);
    }

    public void Refresh(UIState state)
    {
        if (state.Composite == null)
        {
            _stateRow.Present = false;
            _decoderRow.Present = false;
            _timeEstRow.Present = false;
            _timeCompleteRow.Present = false;
            // Drop rows stay present - they track activity even before
            // the first composite is loaded.
        }
        else
        {
            _stateRow.Present = true;
            _decoderRow.Present = true;
            _timeEstRow.Present = true;
            _timeCompleteRow.Present = true;

            var composite = state.Composite;
            _stateValue.Text = composite.State.Description();
            _decoderValue.Text = composite.DecoderName ?? "-";
            _timeEstValue.Text = Formatters.MsToStr(composite.LoadTimeEstimated);
            _timeCompleteValue.Text = Formatters.MsToStr(composite.LoadTimeComplete);
        }

        var app = state.AppStates;

        if (app.DropAborted)
        {
            _dropStatusRow.Present = true;
            _dropStatusValue.Text = "Aborted";
        }
        else if (app.DropActive)
        {
            _dropStatusRow.Present = true;
            _dropStatusValue.Text = "Active";
        }
        else
        {
            _dropStatusRow.Present = false;
        }

        _dropEnqueuedValue.Text = app.DropPathsEnqueued.ToString();
        _dropFilesValue.Text = app.DropFilesEnumerated.ToString();
        _dropSupportedValue.Text = app.DropFilesSupported.ToString();

        RefreshRecord(state.CurrentRecord);
    }

    private void RefreshRecord(FileRecord? current)
    {
        var present = current is not null;
        _recordNameRow.Present = present;
        _recordDirRow.Present = present;
        _recordSizeRow.Present = present;
        _recordContentHashRow.Present = present;
        _recordPHashRow.Present = present;
        _recordGroupRow.Present = present;

        if (current is not { } record)
            return;

        _recordNameValue.Text = record.Name;
        _recordDirValue.Text = record.Directory;
        _recordSizeValue.Text = record.HasSize ? $"{record.Size:N0} B" : "-";
        _recordContentHashValue.Text = record.HasContentHash ? record.ContentHash.ToString("X") : "-";
        _recordPHashValue.Text = record.HasPHash ? record.PHash.ToString("X16") : "-";
        _recordGroupValue.Text = record.HasGroup ? record.GroupId!.Value.ToString() : "-";
    }

    public void SetPointer(float x, float y) => _pointerValue.Text = $"{x:F1}, {y:F1}";

    public void SetHover(IComponent? hovered)
    {
        var description = hovered switch
        {
            Button btn => $"Button \"{btn.Text}\"",
            null       => "-",
            _          => hovered.GetType().Name
        };
        SetHit(description);
    }
    
    public void SetHit(string hitDescription) => _hitValue.Text = hitDescription;

    public void SetAction(string actionDescription) => _actionValue.Text = actionDescription;

    private static HStack BuildRow(
        List<Label> keyLabelCollector,
        string key,
        string initialValue,
        out Label valueLabel)
    {
        var keyLabel = new Label(key)
        {
            Color = Palette.Dim,
            HorizontalSize = SizeMode.Fixed,
            // Width is set in the constructor after all keys are collected.
            Transient = true
        };
        keyLabelCollector.Add(keyLabel);

        valueLabel = new Label(initialValue)
        {
            Color = Palette.Dim,
            Transient = true
        };

        var row = new HStack
        {
            Spacing = 8,
            HorizontalSize = SizeMode.Expand,
            HorizontalAlign = HAlign.Left
        };
        row.AddComponents(keyLabel, valueLabel);
        return row;
    }

    private static Label Spacer() => new(" ")
    {
        Color = Palette.Dim,
        Transient = true
    };
}