program readinput(input, output);
var i, j, k: integer; x, y: real; c, d: char; s: integer;
begin
   read(i, j); readln(k);
   writeln(i:1, ' ', j:1, ' ', k:1);
   read(x, y); writeln(x:6:2, y:10:4);
   readln;
   read(c, d); writeln('[', c, '][', d, ']', eoln);
   readln;
   s := 0;
   while not eof do
   begin
      read(i); s := s + i;
      if eoln then readln
   end;
   writeln('sum ', s:1)
end.
