program pointers(output);
type ip = ^integer; ipp = ^ip;
     vec = array[1..3] of real; vp = ^vec;
     cell = record v: integer; next: ^cell end;
var p, q: ip; pp: ipp; a: vp; c, d: ^cell; i: integer;
begin
   new(p); p^ := 7; q := p; q^ := q^ + 1; writeln(p^:2, p = q, p <> nil, nil = q);
   new(pp); pp^ := p; pp^^ := 100; writeln(p^:4);
   new(q); q^ := 1; writeln(p = q, p^ + q^:4);
   new(a); for i := 1 to 3 do a^[i] := i / 2; writeln(a^[3]:4:1);
   c := nil;
   for i := 1 to 3 do begin new(d); d^.v := i; d^.next := c; c := d end;
   while c <> nil do begin write(c^.v:2); d := c; c := c^.next; dispose(d) end; writeln;
   dispose(p); p := nil; writeln(p = nil)
end.
