program binfiles(output);
type person = record name: alfa; age: integer end;
     color = (red, green, blue);
var f: file of person; p: person; n: file of integer; r: file of real;
    s: file of set of color; cs: set of color; i, total: integer; x: real; k: file of char; c: char;
begin
   rewrite(f);
   p.name := 'Ada       '; p.age := 36; write(f, p);
   p.name := 'Niklaus   '; p.age := 50; f^ := p; put(f);
   reset(f);
   while not eof(f) do begin read(f, p); writeln(p.name, p.age:4) end;
   rewrite(n); for i := 1 to 5 do write(n, i); reset(n); total := 0;
   while not eof(n) do begin total := total + n^; get(n) end; writeln(total:3);
   reset(n); read(n, x, x); writeln(x:4:1);
   rewrite(r); write(r, 1.5, 2); reset(r); read(r, x); writeln(x:4:1, r^:4:1);
   rewrite(s); write(s, [red, blue], []); reset(s); read(s, cs); writeln(blue in cs, green in cs, s^ = []);
   rewrite(k); write(k, 'x', 'y'); reset(k); read(k, c); writeln(c, k^, eof(k));
   get(k); writeln(eof(k))
end.
