program fileerrors(output);
type rec = record f: text end;
     ff = file of text;
     fp = file of ^integer;
var f, g: text; n: file of integer; r: rec; i: integer;
procedure p(f: text); begin end;
begin
   f := g;
   r := r;
   writeln(n, 1);
   write(f, 1:2:3);
   read(f, i:3);
   eoln(n);
   get(i);
   reset(i)
end.
