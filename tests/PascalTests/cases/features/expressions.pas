program expressions(output);
var a, b, c: integer; x: real; p, q: boolean;
begin
   a := 2; b := 3; c := 4;
   writeln(a + b * c:3, (a + b) * c:3, -a * b:3, -(a * b):3, a - (-b):3);
   writeln(a * b div c:3, a * (b div c):3, c mod b * a:3);
   x := a / b * c; writeln(x:8:4);
   p := (a < b) and (b < c); q := not p or (a = 2);
   writeln(p, q, (a > b) = (b > c), not (a = b));
   writeln(a + 0.5:5:1, (a + b) / 2:5:1, sqr(a + b):3, ord(a < b) + 1:3)
end.
