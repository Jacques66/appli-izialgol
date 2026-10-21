program routines(output);
type arr = array[1..3] of integer;
var i: integer; a: arr;
function bad: arr; begin end;
procedure fwd(x: integer); forward;
procedure fwd2(x: integer); forward;
procedure fwd2(x: integer); begin end;
procedure p(x, y: integer); begin end;
function f(x: integer): integer; begin f := x end;
begin
   i := p(1, 2);
   f(1);
   p(1);
   p(1, 2, 3);
   i := f;
   f := 3
end.
