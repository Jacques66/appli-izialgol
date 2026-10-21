program integers(output);
var a, b: integer;
begin
   writeln(7 div 2:3, -7 div 2:3, 7 div (-2):3, (-7) div (-2):3);
   writeln(7 mod 2:3, -7 mod 2:3, 7 mod (-2):3, (-7) mod (-2):3);
   writeln(2 + 3 * 4:3, (2 + 3) * 4:3, 10 - 4 - 3:3, 100 div 10 div 5:3);
   a := 140737488355328;          { 2^47 }
   b := a + (a - 1);              { maxint = 2^48 - 1 }
   writeln(b:1, ' ', b = maxint);
   writeln(abs(-12):1, ' ', sqr(-12):1, ' ', odd(-3), ' ', odd(4));
   a := 16777215; writeln(a * 16777217:1);
   writeln(-maxint div 2:1)
end.
