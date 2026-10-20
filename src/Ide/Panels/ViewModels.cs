using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Studio.Common;
using Studio.Ide.Language;

namespace Studio.Ide.Panels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Row of the error list.</summary>
public sealed class ErrorItem
{
    public ErrorItem(Severity severity, string message, string file, int line, int column, bool runtime)
    {
        Severity = severity; Message = message; File = file; Line = line; Column = column; IsRuntime = runtime;
    }
    public Severity Severity { get; }
    public string Message { get; }
    public string File { get; }
    public int Line { get; }
    public int Column { get; }
    public bool IsRuntime { get; }
    public string FileName => string.IsNullOrEmpty(File) ? "" : System.IO.Path.GetFileName(File);
    public string LineText => Line > 0 ? Line.ToString() : "";
    public string ColumnText => Column > 0 ? Column.ToString() : "";
    public string Origin => IsRuntime ? "Runtime" : "Compiler";
    public Geometry Icon => (Geometry)Application.Current.Resources[Severity switch
    {
        Severity.Error => "Icon.ErrorMark",
        Severity.Warning => "Icon.WarningMark",
        _ => "Icon.InfoMark",
    }];
    public Brush IconBrush => (Brush)Application.Current.Resources[Severity switch
    {
        Severity.Error => "Brush.Error",
        Severity.Warning => "Brush.Warning",
        _ => "Brush.Info",
    }];
}

/// <summary>Row of the call stack.</summary>
public sealed class StackItem : Observable
{
    bool _isSelectedFrame;
    public StackItem(int index, StackFrameInfo frame) { Index = index; Frame = frame; }
    public int Index { get; }
    public StackFrameInfo Frame { get; }
    public string Name => string.IsNullOrEmpty(Frame.Name) ? "?" : Frame.Name;
    public string FileName => string.IsNullOrEmpty(Frame.File) ? "" : System.IO.Path.GetFileName(Frame.File);
    public string LineText => Frame.Line > 0 ? Frame.Line.ToString() : "";
    public string ColumnText => Frame.Column > 0 ? Frame.Column.ToString() : "";
    public bool IsSelectedFrame { get => _isSelectedFrame; set { _isSelectedFrame = value; Notify(); Notify(nameof(MarkerVisibility)); } }
    public Visibility MarkerVisibility => IsSelectedFrame ? Visibility.Visible : Visibility.Hidden;
    public Brush MarkerBrush => (Brush)Application.Current.Resources[Index == 0 ? "Brush.IconYellow" : "Brush.IconGreen"];
}

/// <summary>Node of the variables tree (children loaded on demand).</summary>
public sealed class VariableNode : Observable
{
    readonly Func<VariableInfo, IReadOnlyList<VariableInfo>>? _loadChildren;
    bool _isExpanded, _loaded;

    public VariableNode(VariableInfo info, string path, Func<VariableInfo, IReadOnlyList<VariableInfo>>? loadChildren)
    {
        Info = info;
        Path = path;
        _loadChildren = loadChildren;
        if (info.HasChildren) Children.Add(Placeholder);
    }

    VariableNode(string text)
    {
        Info = new VariableInfo(text, "", "");
        Path = "";
        _loaded = true;
    }

    static VariableNode Placeholder => new("loading…");

    public VariableInfo Info { get; }
    public string Path { get; }
    public string Name => Info.Name;
    public string Value => Info.Value;
    public string Type => string.IsNullOrEmpty(Info.Type) ? "" : Info.Type;
    public string TypeSuffix => string.IsNullOrEmpty(Info.Type) ? "" : "   (" + Info.Type + ")";
    public ObservableCollection<VariableNode> Children { get; } = new();

    /// <summary>Expanded paths (kept between two pauses).</summary>
    public HashSet<string>? ExpandedPaths { get; set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            if (value) { LoadChildren(); ExpandedPaths?.Add(Path); }
            else ExpandedPaths?.Remove(Path);
            Notify();
        }
    }

    void LoadChildren()
    {
        if (_loaded || _loadChildren == null) return;
        _loaded = true;
        Children.Clear();
        try
        {
            int i = 0;
            foreach (var c in _loadChildren(Info))
            {
                var node = new VariableNode(c, Path + "/" + c.Name + "#" + i++, _loadChildren) { ExpandedPaths = ExpandedPaths };
                Children.Add(node);
                if (ExpandedPaths != null && ExpandedPaths.Contains(node.Path)) node.IsExpanded = true;
            }
        }
        catch (Exception ex)
        {
            Children.Add(new VariableNode("error: " + ex.Message));
        }
    }
}

/// <summary>Item of the Outline panel.</summary>
public sealed class OutlineEntry
{
    public OutlineEntry(OutlineItem item, int line)
    {
        Item = item;
        Line = line;
    }
    public OutlineItem Item { get; }
    public int Line { get; }
    public string Name => Item.Name;
    public string Detail => Item.Detail;
    public Thickness Indent => new(Item.Depth * 14, 0, 0, 0);
    public string Letter => Item.Kind switch
    {
        OutlineKind.Class => "C",
        OutlineKind.Operator => "O",
        OutlineKind.Mode => "M",
        OutlineKind.Label => "L",
        _ => "P",
    };
    public Brush Badge => (Brush)Application.Current.Resources[Item.Kind switch
    {
        OutlineKind.Class => "Brush.IconOrange",
        OutlineKind.Operator => "Brush.IconGreen",
        OutlineKind.Mode => "Brush.IconBlue",
        _ => "Brush.IconPurple",
    }];
    public string ToolTipText => $"{Detail} {Name} — line {Line}";
}

/// <summary>Node of the file explorer (lazy loading).</summary>
public sealed class FileNode : Observable
{
    bool _isExpanded, _loaded;

    public FileNode(string fullPath, bool isDirectory)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Name = System.IO.Path.GetFileName(fullPath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(Name)) Name = fullPath;
        if (isDirectory) Children.Add(new FileNode());
    }

    FileNode() { FullPath = ""; Name = "…"; _loaded = true; }

    public string FullPath { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    public ObservableCollection<FileNode> Children { get; } = new();

    public string Extension => IsDirectory ? "" : System.IO.Path.GetExtension(FullPath).ToLowerInvariant();
    public string Badge => IsDirectory ? "" : Extension switch { ".sim" => "S", ".a68" => "A", ".echo" => "E", ".md" => "M", _ => "" };
    public Brush IconBrush => (Brush)Application.Current.Resources[IsDirectory ? "Brush.IconYellow" : Extension switch
    {
        ".sim" => "Brush.IconBlue",
        ".a68" => "Brush.IconGreen",
        ".echo" => "Brush.IconPurple",
        _ => "Brush.IconGray",
    }];
    public Geometry Icon => (Geometry)Application.Current.Resources[IsDirectory ? "Icon.Folder" : "Icon.File"];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            if (value) Load();
            Notify();
        }
    }

    public void Load(bool force = false)
    {
        if (!IsDirectory || (_loaded && !force)) return;
        _loaded = true;
        Children.Clear();
        try
        {
            var di = new System.IO.DirectoryInfo(FullPath);
            foreach (var d in di.EnumerateDirectories().Where(d => !d.Name.StartsWith('.') && (d.Attributes & System.IO.FileAttributes.Hidden) == 0
                                                                  && d.Name is not ("bin" or "obj")).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                Children.Add(new FileNode(d.FullName, true));
            foreach (var f in di.EnumerateFiles().Where(f => !f.Name.StartsWith('.') && (f.Attributes & System.IO.FileAttributes.Hidden) == 0)
                                                 .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                Children.Add(new FileNode(f.FullName, false));
        }
        catch (Exception ex)
        {
            Children.Add(new FileNode("(" + ex.Message + ")", false));
        }
    }
}
