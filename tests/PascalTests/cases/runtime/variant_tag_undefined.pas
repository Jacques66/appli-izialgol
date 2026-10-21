program tagundefined(output);
type fig = record case k: boolean of true: (r: real); false: (n: integer) end;
var f: fig;
begin
   f.n := 3
end.
