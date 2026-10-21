program labels(output);
label 1, 2, 3, 12345;
var i: integer;
begin
   goto 4;
   goto 2;
1: i := 1;
1: i := 2;
   if i = 1 then begin 3: i := 3 end;
   goto 3;
   5: i := 0
end.
