program gotoinactive(output);
label 1;
var i: integer;
procedure p; begin goto 1 end;
begin
   i := 0;
   p;
   while i < 3 do
   begin
      i := i + 1;
1:    writeln(i:1)
   end
end.
