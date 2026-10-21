program withstmt(output);
type inner = record a, b: integer end;
     outer = record a: integer; x: inner; c: char end;
var r: outer; arr: array[1..3] of inner; i: integer; p: ^outer;
begin
   with r do begin a := 1; c := 'c'; with x do begin a := 2; b := 3 end end;
   writeln(r.a:2, r.x.a:2, r.x.b:2, r.c:2);
   with r, x do begin a := 20; b := 30 end;
   writeln(r.a:3, r.x.a:3, r.x.b:3);
   i := 1;
   with arr[i] do begin a := 100; b := i + 1 end;
   writeln(arr[1].a:4, arr[1].b:4);
   new(p); with p^ do begin a := 5; x.a := 6; x.b := 7; c := 'p' end;
   with p^.x do writeln(a:2, b:2, p^.a:2, p^.c:2)
end.
