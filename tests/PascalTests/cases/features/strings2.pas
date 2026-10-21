program strings2(output);
type name = packed array[1..5] of char;
var a, b: name; x: alfa; u: array[1..5] of char; i: integer;
begin
   a := 'apple'; b := 'apply';
   writeln(a < b, a = b, a <> b, b >= a, 'abc' < 'abd', 'b' > 'a');
   x := 'Pascal6000'; writeln(x, ' ', x[7], x[10]);
   unpack(a, u, 1);
   for i := 1 to 5 do u[i] := chr(ord(u[i]) - ord('a') + ord('A'));
   pack(u, 1, b); writeln(b);
   a[1] := 'A'; writeln(a = b, a < b);
   writeln(a:7, '|', a:2, '|')
end.
