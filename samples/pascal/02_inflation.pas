{ Inflation table, after the User Manual (Jensen and Wirth, chapter 3):
  the factors by which prices rise in n years at 7, 8 and 10 per cent. }
program inflation(output);
const n = 10;
var i: integer;
    w1, w2, w3: real;
begin
   writeln(' year      7%        8%       10%');
   i := 0; w1 := 1.0; w2 := 1.0; w3 := 1.0;
   repeat
      i := i + 1;
      w1 := w1 * 1.07;
      w2 := w2 * 1.08;
      w3 := w3 * 1.10;
      writeln(i:5, w1:10:4, w2:10:4, w3:10:4)
   until i = n
end.
