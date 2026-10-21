{ Standard identifiers belong to an outer block: they can be redeclared. }
program redefined(output);
const maxint = 100;
var true: boolean; sqr: integer;
function sin(x: integer): integer; begin sin := x + 1 end;
procedure write(n: integer); begin writeln('my write ', n:1) end;
begin
   true := false; sqr := 4;
   write(maxint); writeln(true, sqr:2, sin(1):2)
end.
