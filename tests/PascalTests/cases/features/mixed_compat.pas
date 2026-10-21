program compat(output);
type s1 = 1..10; s2 = 5..20; letters = 'a'..'z';
var a: s1; b: s2; i: integer; r: real; l: letters; c: char;
begin
   a := 7; b := a; i := b; r := i; r := a + b;
   writeln(a:3, b:3, i:3, r:5:1);
   c := 'q'; l := c; writeln(l, ord(l) - ord('a'):3);
   a := b - 5; writeln(a:3)
end.
