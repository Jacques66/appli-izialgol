program scopes(output);
var x: integer; abs: integer;
procedure p;
var x: real;
   procedure q;
   var x: char;
   begin x := 'q'; writeln(x) end;
begin x := 1.5; q; writeln(x:4:1) end;
procedure r;
type integer = char;
var i: integer;
begin i := 'r'; writeln(i) end;
begin
   x := 7; abs := -3; p; r; writeln(x:2, abs:3)
end.
