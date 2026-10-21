program operators(output);
type rec = record a: integer end;
var i: integer; r: real; b: boolean; x, y: rec; p: ^integer; s: set of 0..9;
begin
   i := 7.0 div 2;
   i := i mod r;
   r := b + 1;
   b := x = y;
   b := p < nil;
   b := i < 'a';
   i := -b;
   b := not i;
   i := i * -2;
   b := 1 < 2 < 3;
   i := 3 div 0
end.
