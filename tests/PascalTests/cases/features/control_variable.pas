program controlvar(output);
var i, s: integer;
procedure p;
var i: integer;
begin for i := 1 to 3 do s := s + i end;
begin
   s := 0;
   for i := 1 to 4 do begin p; s := s + 10 * i end;
   writeln(s:1)
end.
