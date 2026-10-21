{ Copying a record whose fields are partly undefined is allowed. }
program partcopy(output);
type r = record a, b: integer end;
var x, y: r;
begin
   x.a := 1; y := x; writeln(y.a:1)
end.
