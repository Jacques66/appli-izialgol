program newvariants(output);
type kind = (circle, square);
     fig = record
             name: char;
             case k: kind of
                circle: (r: integer);
                square: (s: integer)
           end;
var p, q: ^fig;
begin
   new(p, circle); p^.name := 'c'; p^.r := 3;
   new(q, square); q^.name := 's'; q^.s := 4;
   writeln(p^.name, ord(p^.k):2, p^.r:2, q^.name, ord(q^.k):2, q^.s:2);
   dispose(p, circle); dispose(q, square);
   writeln('done')
end.
