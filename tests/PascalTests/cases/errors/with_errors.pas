program witherrors(output);
var i: integer; r: record a: integer end;
begin
   with i do;
   with r do b := 1;
   r.b := 2;
   i.a := 3;
   i[1] := 0;
   i^ := 0
end.
