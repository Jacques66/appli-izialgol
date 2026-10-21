program booleans(output);
var a, b: boolean; i: integer;
begin
   for i := 0 to 3 do
   begin
      a := odd(i); b := i > 1;
      writeln(a:6, b:6, a and b:6, a or b:6, not a:6, a <> b:6, a <= b:6)
   end;
   writeln(true, false, true:3, false:1)
end.
