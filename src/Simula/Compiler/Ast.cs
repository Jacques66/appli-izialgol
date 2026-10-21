namespace Studio.Simula.Compiler;

// ---------------------------------------------------------------- syntactic types

/// <summary>Source language handled by the shared compiler.</summary>
public enum Dialect { Simula67, Algol60, SimulaI }

public enum BaseType { None, Integer, ShortInteger, Real, LongReal, Boolean, Character, Text, Ref, Label, Switch, Notype }

public sealed class TypeSpec
{
    public BaseType Base;
    public string? RefClass;     // for ref(C)
    public SrcPos Pos;
    public static TypeSpec Of(BaseType b, SrcPos p) => new() { Base = b, Pos = p };
    public override string ToString() => Base switch
    {
        BaseType.Ref => $"ref({RefClass})",
        BaseType.ShortInteger => "short integer",
        BaseType.LongReal => "long real",
        _ => Base.ToString().ToLowerInvariant()
    };
}

// ---------------------------------------------------------------- nodes

public abstract class Node
{
    public SrcPos Pos;
}

// ---------------------------------------------------------------- declarations

public abstract class Decl : Node { }

public sealed class VarDecl : Decl
{
    public bool IsOwn;
    public TypeSpec Type = null!;
    public List<(string Name, string Orig, SrcPos Pos, Expr? Const)> Items = new();
}

public sealed class BoundPair { public Expr Lower = null!, Upper = null!; }

public sealed class ArraySegment
{
    public List<(string Name, string Orig, SrcPos Pos)> Names = new();
    public List<BoundPair> Bounds = new();
}

public sealed class ArrayDecl : Decl
{
    public bool IsOwn;
    public TypeSpec Type = null!;   // Real when omitted
    public List<ArraySegment> Segments = new();
}

public sealed class SwitchDecl : Decl
{
    public string Name = "", Orig = "";
    public List<Expr> Items = new();
}

public enum ParamMode { Default, Value, Name }

public enum ParamKind { Simple, Array, Procedure, Label, Switch }

public sealed class ParamSpec
{
    public string Name = "", Orig = "";
    public SrcPos Pos;
    public ParamMode Mode;
    public ParamKind Kind;
    public TypeSpec? Type;       // null: unspecified / untyped (proper procedure, label, switch)
    public bool Specified;
}

public sealed class ProcDecl : Decl
{
    public string Name = "", Orig = "";
    public TypeSpec? Type;          // null = proper procedure
    public List<ParamSpec> Params = new();
    public Stmt? Body;              // null for a specification (virtual ... is) or external
    public bool IsExternalSpec;
    public string? NativeKind;      // external <kind> procedure
}

public sealed class VirtualSpec
{
    public string Name = "", Orig = "";
    public SrcPos Pos;
    public ParamKind Kind;           // Simple for an unspecified 'procedure'; Label; Switch
    public bool IsProcedure;
    public TypeSpec? Type;
    public ProcDecl? Heading;        // virtual: procedure p is procedure p(..); ...;
}

public sealed class ClassDecl : Decl
{
    public string Name = "", Orig = "";
    public string? Prefix;
    public SrcPos PrefixPos;
    public List<ParamSpec> Params = new();
    public List<(string Name, SrcPos Pos)> Hidden = new();
    public List<(string Name, SrcPos Pos)> Protected = new();
    public List<VirtualSpec> Virtuals = new();
    public List<Decl> Decls = new();
    public List<Stmt> Before = new();    // statements before inner
    public List<Stmt> After = new();     // statements after inner
    public bool HasInner;
    public SrcPos InnerPos;
    public List<string> InnerLabels = new();
    public SrcPos EndPos;
    public bool FromPrelude;
}

public sealed class ExternalDecl : Decl
{
    public bool IsClass;
    public string? Kind;
    public TypeSpec? Type;
    public List<(string Name, string? Ident, SrcPos Pos)> Items = new();
    public ProcDecl? Spec;
}

// ---------------------------------------------------------------- statements

public abstract class Stmt : Node
{
    public List<(string Name, string Orig, SrcPos Pos)> Labels = new();
}

public sealed class DummyStmt : Stmt { }

public sealed class BlockStmt : Stmt
{
    public List<Decl> Decls = new();
    public List<Stmt> Stmts = new();
    public bool IsCompound => Decls.Count == 0;
    // prefixed block
    public string? Prefix;
    public List<Expr> PrefixArgs = new();
    public SrcPos PrefixPos;
    public SrcPos EndPos;
    public bool IsProgramBlock;     // environment block
}

public sealed class AssignStmt : Stmt
{
    // a := b := expr; each target with its operator
    public List<(Expr Target, bool IsRef)> Targets = new();
    public Expr Value = null!;
}

public sealed class ExprStmt : Stmt { public Expr E = null!; }   // procedure call, new C(...)

public sealed class IfStmt : Stmt
{
    public Expr Cond = null!;
    public Stmt Then = null!;
    public Stmt? Else;
}

public sealed class WhileStmt : Stmt { public Expr Cond = null!; public Stmt Body = null!; }

public enum ForKind { Value, StepUntil, While }

public sealed class ForElem
{
    public ForKind Kind;
    public Expr A = null!, B = null!, C = null!; // Value: A ; StepUntil: A step B until C ; While: A while B
    public SrcPos Pos;
}

public sealed class ForStmt : Stmt
{
    public Expr Var = null!;
    public bool IsRef;
    public List<ForElem> Elems = new();
    public Stmt Body = null!;
}

public sealed class GotoStmt : Stmt { public Expr Target = null!; }

public sealed class WhenClause { public string Class = ""; public SrcPos Pos; public Stmt Body = null!; }

public sealed class InspectStmt : Stmt
{
    public Expr Obj = null!;
    public Stmt? DoBody;
    public List<WhenClause> Whens = new();
    public Stmt? Otherwise;
}

public sealed class InnerStmt : Stmt { }

public sealed class ActivateStmt : Stmt
{
    public bool Reactivate;
    public Expr Obj = null!;
    public string Code = "direct";    // direct, at, delay, before, after
    public Expr? Time;
    public Expr? Other;
    public bool Prior;
}

// ---------------------------------------------------------------- expressions

public abstract class Expr : Node { }

public sealed class IntLit : Expr { public long Value; }
public sealed class RealLit : Expr { public double Value; public bool IsLong; }
public sealed class BoolLit : Expr { public bool Value; }
public sealed class CharLit : Expr { public char Value; }
public sealed class TextLit : Expr { public string Value = ""; }
public sealed class NoneLit : Expr { }
public sealed class NotextLit : Expr { }

public sealed class Ident : Expr { public string Name = "", Orig = ""; }

/// <summary>x(args): procedure call, array element or switch element.</summary>
public sealed class CallExpr : Expr { public Expr Fn = null!; public List<Expr> Args = new(); }

public sealed class DotExpr : Expr { public Expr Obj = null!; public string Name = "", Orig = ""; public SrcPos NamePos; }

public sealed class BinExpr : Expr { public T Op; public Expr L = null!, R = null!; }

public sealed class UnExpr : Expr { public T Op; public Expr E = null!; }   // Plus, Minus, KNot

public sealed class IfExpr : Expr { public Expr Cond = null!, Then = null!, Else = null!; }

public sealed class NewExpr : Expr { public string Class = "", Orig = ""; public List<Expr> Args = new(); public bool ElementWrapped; }

public sealed class ThisExpr : Expr { public string Class = "", Orig = ""; }

public sealed class QuaExpr : Expr { public Expr Obj = null!; public string Class = "", Orig = ""; }

public sealed class IsInExpr : Expr { public Expr Obj = null!; public string Class = "", Orig = ""; public bool IsIn; }
