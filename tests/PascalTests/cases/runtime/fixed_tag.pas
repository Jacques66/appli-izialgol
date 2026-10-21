program fixedtag(output);
type shape = (circle, square);
     fig = record case k: shape of circle: (r: real); square: (s: real) end;
var p: ^fig;
begin
   new(p, circle); p^.r := 1;
   writeln(p^.r:4:1);
   p^.k := square      { Revised Report 10.1.2: the tag fields must remain constant }
end.
