program packing(output);
var a: array[1..10] of char; z: packed array[1..4] of char; i: integer;
    n: array[0..5] of integer; pn: packed array[1..3] of integer;
begin
   for i := 1 to 10 do a[i] := chr(ord('a') + i - 1);
   pack(a, 3, z); writeln(z);
   z := 'WXYZ'; unpack(z, a, 7);
   for i := 1 to 10 do write(a[i]); writeln;
   for i := 0 to 5 do n[i] := i * i;
   pack(n, 2, pn); writeln(pn[1]:3, pn[2]:3, pn[3]:3);
   pn[2] := -1; unpack(pn, n, 0); writeln(n[0]:3, n[1]:3, n[2]:3, n[3]:3)
end.
