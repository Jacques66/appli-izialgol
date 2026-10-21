program subranges(output);
type digit = '0'..'9'; index = 1..100; small = -5..5;
var c: digit; i: index; s: small; total: integer;
begin
   total := 0;
   for c := '0' to '9' do total := total + ord(c) - ord('0');
   writeln(total:1);
   i := 100; s := -5; total := i * s; writeln(total:1);
   s := i div 25 + 1; writeln(s:1);
   c := '7'; writeln(c, ord(c) - ord('0'):2)
end.
