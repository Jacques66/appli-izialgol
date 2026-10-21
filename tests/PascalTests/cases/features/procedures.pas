program procedures(output);
var g, x, y: integer;
procedure swap(var a, b: integer);
var t: integer;
begin t := a; a := b; b := t end;
procedure byvalue(a: integer);
begin a := a * 2; writeln('inside ', a:1) end;
procedure outer(n: integer);
var local: integer;
   procedure inner(m: integer);
   begin local := local + m; g := g + 1 end;
begin
   local := 0; inner(n); inner(n * 10); writeln('local ', local:1)
end;
begin
   x := 1; y := 2; swap(x, y); writeln(x:2, y:2);
   byvalue(x); writeln('outside ', x:1);
   g := 0; outer(3); writeln('g ', g:1)
end.
