program packerrors(output);
var a: array[1..3] of char; z: packed array[1..5] of char; n: packed array[1..2] of integer;
begin
   pack(a, 1, z);
   pack(z, 1, a);
   pack(a, 'x', n);
   unpack(a, z, 1)
end.
