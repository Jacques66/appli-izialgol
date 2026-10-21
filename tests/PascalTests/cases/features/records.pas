program records(output);
type point = record x, y: integer end;
     line = record a, b: point; name: char end;
var p, q: point; l, m: line;
procedure shift(p: point; var r: point);
begin p.x := p.x + 100; r := p end;
function len2(l: line): integer;
begin len2 := sqr(l.b.x - l.a.x) + sqr(l.b.y - l.a.y) end;
begin
   p.x := 1; p.y := 2; q := p; q.x := 5;
   writeln(p.x:2, p.y:2, q.x:2, q.y:2);
   l.a := p; l.b := q; l.name := 'L'; m := l; m.b.y := 10;
   writeln(l.b.y:3, m.b.y:3, m.name:2, len2(l):3, len2(m):3);
   shift(p, q); writeln(p.x:4, q.x:4)
end.
