program longout(output);
var i: integer;
begin
   for i := 1 to 3000 do begin write(i mod 10:1); if i mod 100 = 0 then writeln end
end.
