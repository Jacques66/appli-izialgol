using System.Text;

namespace Studio.Pascal.Semantics;

public enum TK { Integer, Real, Boolean, Char, Enum, Subrange, Array, Record, Set, File, Pointer, Nil, Error }

/// <summary>Pascal type. Type identity is object identity (a type definition "type a = b" makes a an alias).</summary>
public abstract class PType
{
    /// <summary>Name of the type identifier that denotes it (for messages), null for anonymous types.</summary>
    public string? Name;
    public abstract TK Kind { get; }
    /// <summary>Number of storage cells.</summary>
    public virtual int Size => 1;
    public bool Packed;

    public bool IsOrdinal => Kind is TK.Integer or TK.Boolean or TK.Char or TK.Enum or TK.Subrange;
    public bool IsError => Kind == TK.Error;
    /// <summary>Host type of a subrange (the type itself otherwise).</summary>
    public virtual PType Host => this;
    public virtual long Lo => 0;
    public virtual long Hi => 0;
    public bool IsInteger => Host.Kind == TK.Integer;
    public bool IsReal => Kind == TK.Real;
    public bool IsNumeric => IsInteger || IsReal;
    public bool IsBoolean => Host.Kind == TK.Boolean;
    public bool IsChar => Host.Kind == TK.Char;
    public bool IsStructured => Kind is TK.Array or TK.Record;

    /// <summary>True for packed array[1..n] of char (n >= 1).</summary>
    public bool IsString => this is ArrayType a && a.Packed && a.Elem.Kind == TK.Char && a.Index.Kind == TK.Subrange && a.Index.Host.Kind == TK.Integer && a.Index.Lo == 1;
    public int StringLength => this is ArrayType a ? (int)a.Count : 0;

    public virtual bool ContainsFile => false;
    public virtual bool ContainsPointer => false;

    /// <summary>Cells holding a file (offset, type) inside a variable of this type.</summary>
    public virtual void CollectFiles(int baseOffset, List<(int, FileType)> list) { }

    /// <summary>Tag fields of variant parts (offset), for the debugger and checks.</summary>
    public override string ToString() => Name ?? Describe();
    public abstract string Describe();
}

public sealed class ErrorType : PType
{
    public static readonly ErrorType T = new();
    public override TK Kind => TK.Error;
    public override string Describe() => "<error>";
}

public sealed class IntegerType : PType
{
    public static readonly IntegerType T = new() { Name = "integer" };
    public override TK Kind => TK.Integer;
    public override long Lo => -Runtime.RT.MaxInt;
    public override long Hi => Runtime.RT.MaxInt;
    public override string Describe() => "integer";
}

public sealed class RealType : PType
{
    public static readonly RealType T = new() { Name = "real" };
    public override TK Kind => TK.Real;
    public override string Describe() => "real";
}

public sealed class CharType : PType
{
    public static readonly CharType T = new() { Name = "char" };
    public override TK Kind => TK.Char;
    public override long Lo => 0;
    public override long Hi => 255;
    public override string Describe() => "char";
}

/// <summary>Enumerated type (boolean is the enumeration (false, true)).</summary>
public sealed class EnumType : PType
{
    public static readonly EnumType Boolean = new(new List<string> { "false", "true" }, true) { Name = "boolean" };
    public readonly List<string> Names;
    readonly bool isBool;
    public EnumType(List<string> names, bool isBool = false) { Names = names; this.isBool = isBool; }
    public override TK Kind => isBool ? TK.Boolean : TK.Enum;
    public override long Lo => 0;
    public override long Hi => Names.Count - 1;
    public override string Describe() => "(" + string.Join(", ", Names) + ")";
}

public sealed class SubrangeType : PType
{
    readonly PType host; readonly long lo, hi;
    public SubrangeType(PType host, long lo, long hi) { this.host = host.Host; this.lo = lo; this.hi = hi; }
    public override TK Kind => TK.Subrange;
    public override PType Host => host;
    public override long Lo => lo;
    public override long Hi => hi;
    public override string Describe() => Runtime.Format.OrdText(host, lo) + ".." + Runtime.Format.OrdText(host, hi);
}

public sealed class ArrayType : PType
{
    public readonly PType Index, Elem;
    public ArrayType(PType index, PType elem, bool packed) { Index = index; Elem = elem; Packed = packed; }
    public override TK Kind => TK.Array;
    public long Count => Index.Hi - Index.Lo + 1;
    public override int Size => (int)Math.Min(int.MaxValue, Count * Elem.Size);
    public override bool ContainsFile => Elem.ContainsFile;
    public override bool ContainsPointer => Elem.ContainsPointer;
    public override void CollectFiles(int b, List<(int, FileType)> list)
    {
        if (!Elem.ContainsFile) return;
        for (long i = 0; i < Count; i++) Elem.CollectFiles(b + (int)i * Elem.Size, list);
    }
    public override string Describe() => (Packed ? "packed " : "") + "array[" + Index + "] of " + Elem;
}

public sealed class Field
{
    public string Name = "";
    public PType Type = ErrorType.T;
    public int Offset;
    /// <summary>Variant containing the field (null: fixed part).</summary>
    public Variant? Variant;
    /// <summary>Tag field of this variant part.</summary>
    public VariantPart? TagOf;
    public int Line, Col;
}

public sealed class VariantPart
{
    public Field? Tag;           // null: no tag field ("case t of")
    public PType TagType = ErrorType.T;
    public readonly List<Variant> Variants = new();
    public int Offset, Size;      // cells of all the variants (the tag field is not included)
    public Variant? Parent;       // enclosing variant (nested variant parts)
    public Variant? Find(long tag) => Variants.FirstOrDefault(v => v.Labels.Contains(tag));
}

public sealed class Variant
{
    public readonly HashSet<long> Labels = new();
    public readonly List<Field> Fields = new();
    public VariantPart Owner = null!;
    public VariantPart? Nested;
    public int Offset, Size;
}

public sealed class RecordType : PType
{
    public readonly List<Field> Fields = new();       // every field, including the variants
    public VariantPart? VariantPart;
    int size;
    public RecordType(bool packed) { Packed = packed; }
    public void SetSize(int s) => size = s;
    public override TK Kind => TK.Record;
    public override int Size => size;
    public Field? Find(string name) => Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    public override bool ContainsFile => Fields.Any(f => f.Type.ContainsFile);
    public override bool ContainsPointer => Fields.Any(f => f.Type.ContainsPointer);
    public override void CollectFiles(int b, List<(int, FileType)> list)
    {
        foreach (var f in Fields) f.Type.CollectFiles(b + f.Offset, list);
    }
    public override string Describe()
    {
        var sb = new StringBuilder(Packed ? "packed record " : "record ");
        sb.Append(string.Join("; ", Fields.Take(4).Select(f => f.Name + ": " + f.Type)));
        if (Fields.Count > 4) sb.Append("; ...");
        return sb.Append(" end").ToString();
    }
}

public sealed class SetType : PType
{
    /// <summary>Base type; null for the type of the empty set [].</summary>
    public readonly PType? Base;
    public SetType(PType? b, bool packed) { Base = b; Packed = packed; }
    public static readonly SetType EmptySet = new(null, false);
    public override TK Kind => TK.Set;
    public bool IsCharSet => Base != null && Base.IsChar;
    public override string Describe() => Base == null ? "set of ?" : (Packed ? "packed " : "") + "set of " + Base;
}

public sealed class FileType : PType
{
    public readonly PType Elem;
    public readonly bool IsText;
    public FileType(PType elem, bool isText, bool packed) { Elem = elem; IsText = isText; Packed = packed; }
    public static readonly FileType Text = new(CharType.T, true, false) { Name = "text" };
    public override TK Kind => TK.File;
    public override bool ContainsFile => true;
    public override void CollectFiles(int b, List<(int, FileType)> list) => list.Add((b, this));
    public override string Describe() => IsText ? "text" : (Packed ? "packed " : "") + "file of " + Elem;
}

public sealed class PointerType : PType
{
    public PType Target = ErrorType.T;
    public string? PendingName;     // ^T before T is declared (resolved at the end of the type part)
    public int Line, Col;
    public override TK Kind => TK.Pointer;
    public override bool ContainsPointer => true;
    public override string Describe() => "^" + (PendingName ?? Target.ToString());
}

public sealed class NilType : PType
{
    public static readonly NilType T = new();
    public override TK Kind => TK.Nil;
    public override string Describe() => "nil";
}

public static class TypeRules
{
    /// <summary>Same type. Pointer types are the same when they have the same domain type (as in Pascal 6000:
    /// "var p: ^t; q: ^t" may be assigned to each other); other types follow name equivalence.</summary>
    public static bool Same(PType a, PType b) =>
        ReferenceEquals(a, b) || a.IsError || b.IsError ||
        a is PointerType pa && b is PointerType pb && (ReferenceEquals(pa.Target, pb.Target) || pa.Target.IsError || pb.Target.IsError);

    /// <summary>Compatible types (Revised Report 6.2 and 8.1.5).</summary>
    public static bool Compatible(PType a, PType b)
    {
        if (Same(a, b)) return true;
        if (a.IsOrdinal && b.IsOrdinal) return ReferenceEquals(a.Host, b.Host);
        if (a is SetType sa && b is SetType sb)
            return sa.Base == null || sb.Base == null || ReferenceEquals(sa.Base.Host, sb.Base.Host);
        if (a.IsString && b.IsString) return a.StringLength == b.StringLength;
        if (a.Kind == TK.Pointer && b.Kind == TK.Nil || a.Kind == TK.Nil && b.Kind == TK.Pointer) return true;
        if (a.Kind == TK.Nil && b.Kind == TK.Nil) return true;
        return false;
    }

    /// <summary>Assignment compatibility of a value of type src to a variable of type dst.</summary>
    public static bool AssignCompatible(PType dst, PType src)
    {
        if (dst.IsError || src.IsError) return true;
        if (dst.ContainsFile || src.ContainsFile) return false;
        if (Same(dst, src)) return true;
        if (dst.IsReal && src.IsInteger) return true;
        if (dst.IsReal && src.IsReal) return true;
        if (dst.IsOrdinal && src.IsOrdinal) return ReferenceEquals(dst.Host, src.Host);
        if (dst is SetType && src is SetType) return Compatible(dst, src);
        if (dst.IsString && src.IsString) return dst.StringLength == src.StringLength;
        if (dst.Kind == TK.Pointer && src.Kind == TK.Nil) return true;
        return false;
    }

    /// <summary>The ordinal values of t fit within 0..58 (Pascal 6000 set limit) or t is char-based.</summary>
    public const int MaxSetElement = 58;
}
