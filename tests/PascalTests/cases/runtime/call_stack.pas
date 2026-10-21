program callstack(output);
var a: array[1..3] of integer;
procedure level3(i: integer); begin a[i] := 0 end;
procedure level2(i: integer); begin level3(i * 2) end;
procedure level1; begin level2(1); level2(2) end;
begin
   level1
end.
