program deep(output);
type v3 = array[1..3] of integer;
     r = record name: alfa; data: array[1..2] of v3; next: ^r end;
var a, b: r; p: ^r;
begin
   a.name := 'first     '; a.data[1][1] := 1; a.data[2, 3] := 23; a.next := nil;
   new(p); p^ := a; p^.name := 'second    '; p^.data[2][3] := 99;
   b := p^; b.next := p;
   writeln(a.name, a.data[2, 3]:3, p^.name, p^.data[2, 3]:3, b.next^.name, b.data[1, 1]:2)
end.
