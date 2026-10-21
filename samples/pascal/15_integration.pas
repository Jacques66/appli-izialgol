{ Procedural parameters: numerical integration by Simpson's rule of
  functions passed as parameters, and a root finder (bisection). The formal
  function heading specifies its parameter list; the 1974 form without a
  list (function f: real) is also accepted. }
program integration(output);
const pi = 3.14159265358979;
var k: integer;

function simpson(function f(x: real): real; a, b: real; n: integer): real;
var h, s: real;
    i: integer;
begin
   h := (b - a) / (2 * n);
   s := f(a) + f(b);
   for i := 1 to 2 * n - 1 do
      if odd(i) then s := s + 4 * f(a + i * h)
      else s := s + 2 * f(a + i * h);
   simpson := s * h / 3
end;

function bisect(function f: real; lo, hi, eps: real): real;
var mid: real;
begin
   while hi - lo > eps do
   begin
      mid := (lo + hi) / 2;
      if f(lo) * f(mid) <= 0 then hi := mid else lo := mid
   end;
   bisect := (lo + hi) / 2
end;

function square(x: real): real;
begin square := x * x end;

function sine(x: real): real;
begin sine := sin(x) end;

function gauss(x: real): real;
begin gauss := exp(-x * x / 2) / sqrt(2 * pi) end;

function poly(x: real): real;
begin poly := x * x * x - 2 * x - 5 end;

begin
   writeln('integral of x^2 on [0, 3]       = ', simpson(square, 0, 3, 10):12:8);
   writeln('integral of sin on [0, pi]      = ', simpson(sine, 0, pi, 10):12:8);
   for k := 1 to 3 do
      writeln('integral of the normal density on [-', k:1, ', ', k:1, '] = ', simpson(gauss, -k, k, 50):10:6);
   writeln('root of x^3 - 2x - 5             = ', bisect(poly, 2, 3, 1e-10):14:10)
end.
