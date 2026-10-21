program readeof(input, output);
var i, s: integer;
begin
   s := 0;
   repeat read(i); s := s + i; writeln(s:1) until false
end.
