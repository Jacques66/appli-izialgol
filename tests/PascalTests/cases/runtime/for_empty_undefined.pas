program forempty(output);
{ Revised Report 9.2.3.3: the final value of the control variable is left undefined,
  also when the repeated statement is not executed }
var i: integer;
begin
   i := 5;
   for i := 1 to 0 do writeln('never');
   writeln(i)
end.
