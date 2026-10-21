program newerrors(output);
type k = (x, y);
     rec = record case t: k of x: (a: integer); y: (b: real) end;
var p: ^rec; i: integer;
begin
   new(i);
   new(p, 5);
   new(p, x, y);
   dispose(i)
end.
