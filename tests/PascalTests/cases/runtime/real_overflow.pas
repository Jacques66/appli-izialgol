program realoverflow(output);
var x: real; i: integer;
begin
   x := 1e300;
   for i := 1 to 3 do begin x := x * 1e5; writeln(x) end
end.
