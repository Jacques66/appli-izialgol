program loops2(output);
var i, j, n: integer; c: char;
begin
   for i := 5 downto 1 do write(i:2); writeln;
   for i := 3 to 1 do write('never'); writeln('empty for');
   n := 1; while n < 1000 do n := n * 3; writeln(n:1);
   n := 0; repeat n := n + 1 until n * n > 50; writeln(n:1);
   repeat n := n - 1 until true; writeln(n:1);
   for c := 'x' to 'z' do for i := 1 to 2 do write(c, i:1, ' '); writeln;
   n := 3; j := 0;
   for i := 1 to n do begin n := 10; j := j + 1 end; writeln(j:1);
   for i := maxint - 2 to maxint do write(i - maxint:3); writeln
end.
