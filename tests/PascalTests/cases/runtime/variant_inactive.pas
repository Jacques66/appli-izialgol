program variantinactive(output);
type shape = (circle, square);
     fig = record case k: shape of circle: (r: real); square: (side: real) end;
var f: fig;
begin
   f.k := circle; f.r := 1.0; writeln(f.r:4:1);
   f.k := square; f.side := 2.0; writeln(f.side:4:1);
   writeln(f.r:4:1)
end.
