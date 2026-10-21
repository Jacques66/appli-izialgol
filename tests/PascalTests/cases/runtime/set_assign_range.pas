program setassign(output);
var s: set of 1..10; i: integer;
begin
   i := 10; s := [i]; writeln(10 in s);
   i := 11; s := [1, i]
end.
