program packrange(output);
var a: array[1..5] of char; z: packed array[1..3] of char; i: integer;
begin
   for i := 1 to 5 do a[i] := 'x';
   pack(a, 3, z); writeln(z);
   i := 4; pack(a, i, z)
end.
