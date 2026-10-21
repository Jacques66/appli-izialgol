{ Changing the tag makes the fields of the variants undefined. }
program variantreset(output);
type fig = record case k: boolean of true: (r: integer); false: (n: integer) end;
var f: fig;
begin
   f.k := true; f.r := 3;
   f.k := false; f.k := true;
   writeln(f.r)
end.
