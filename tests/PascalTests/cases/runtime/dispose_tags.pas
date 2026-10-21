program disposetags(output);
type shape = (circle, square);
     fig = record case k: shape of circle: (r: real); square: (s: real) end;
var p: ^fig;
begin
   new(p, circle); p^.r := 1;
   writeln('allocated');
   dispose(p)          { the variable was allocated with a tag value: dispose(p, circle) is required }
end.
