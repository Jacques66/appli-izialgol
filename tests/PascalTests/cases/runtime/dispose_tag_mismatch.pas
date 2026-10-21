program disposemismatch(output);
type shape = (circle, square);
     fig = record case k: shape of circle: (r: real); square: (s: real) end;
var p: ^fig;
begin
   new(p); p^.k := square; p^.s := 1;
   dispose(p, circle)  { the variable holds the variant square }
end.
