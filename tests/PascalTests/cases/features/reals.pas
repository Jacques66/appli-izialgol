program reals(output);
var x, y: real; i: integer;
begin
   x := 1/3; writeln(x);
   writeln(x:12, x:9, x:5, -x:12);
   writeln(x:10:6, -x:10:2, x:1:3);
   writeln(0.0, 0.0:6:2);
   writeln(1e100:15, -2.5e-100:15, 123456789.0:15);
   y := 10; i := 4; writeln(y / i:6:3, i / 8:6:3, i * y:6:1, y - i:6:1);
   writeln(sqrt(16):5:1, sin(0):5:1, cos(0):5:1, exp(1):10:7, ln(exp(2)):5:1, arctan(1) * 4:10:7);
   writeln(trunc(3.99):2, trunc(-3.99):3, round(3.5):2, round(-3.5):3, round(0.49):2);
   writeln(abs(-1.25):5:2, sqr(1.5):6:2, 2.5e0 = 2.5, 1 < 1.5);
   writeln(99.995:8:2, 0.125:6:2, 2.5:1:1, -0.005:6:2, 0.0001:5:3, 123.456:0:1)
end.
