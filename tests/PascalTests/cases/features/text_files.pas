program textfiles(output);
var f, g: text; c: char; n, sum, lines: integer; x: real;
begin
   rewrite(f);
   writeln(f, 'first line');
   writeln(f, 12, -3, 4.5);
   write(f, 'no newline at the end');
   reset(f);
   lines := 0;
   while not eof(f) do begin readln(f); lines := lines + 1 end;
   writeln('lines: ', lines:1);
   reset(f); readln(f); read(f, n, sum, x); writeln(n:3, sum:3, x:5:1, eoln(f));
   readln(f); read(f, c); writeln(c, f^, eoln(f));
   rewrite(g); f^ := 'z';
   g^ := 'A'; put(g); g^ := 'B'; put(g); writeln(g); page(g); write(g, 'C'); writeln(g);
   reset(g);
   while not eof(g) do
   begin
      while not eoln(g) do begin if g^ = chr(12) then write('<FF>') else write(g^); get(g) end;
      writeln('|'); get(g)
   end
end.
