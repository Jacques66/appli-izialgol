program refsymbols(output);
{ the reference symbols of the Report, as in the CDC character set: ≠ ≤ ≥ ¬ ∧ ∨ ↑ }
var a, b: integer; p, q: boolean; r: ↑integer;
begin
   a := 1; b := 2;
   p := (a ≠ b) ∧ (a ≤ b); q := ¬p ∨ (b ≥ a);
   new(r); r↑ := a + b;
   writeln(p, q, a ≠ a, r↑:2)
end.
