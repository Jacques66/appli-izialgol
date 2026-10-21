program disposetags(output);
{ Revised Report 10.1.2: new(p, t1, ..., tn) fixes the tag fields; dispose(p, t1, ..., tn) names the same values }
type shape = (circle, square);
     fig = record
              case k: shape of
                 circle: (r: real; case filled: boolean of true: (c: char); false: ());
                 square: (s: real)
           end;
var p, q, x: ^fig;
begin
   new(p, circle, true); p^.r := 1; p^.c := '*';
   p^.k := circle;                { the same value: allowed }
   new(q); q^.k := square; q^.s := 2; q^.k := circle; q^.r := 5;   { new(p): the tag may change }
   new(x); x^.k := square;
   writeln(p^.r:4:1, p^.c:2, q^.r:4:1);
   dispose(p, circle, true);
   dispose(q, circle);            { the tag value matches the current variant }
   dispose(x);
   writeln('done')
end.
