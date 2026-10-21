program truncround(output);
{ Revised Report 11.1.3: the argument of trunc and round is real }
var i: integer; r: real;
begin
   i := 7; r := 2.5;
   i := trunc(i);
   i := round(3);
   i := trunc(r) + round(r);
   i := round('a')
end.
