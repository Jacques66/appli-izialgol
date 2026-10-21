program nilderef(output);
type link = ^node; node = record v: integer; next: link end;
var p: link;
begin
   new(p); p^.v := 1; p^.next := nil;
   writeln(p^.v:1);
   writeln(p^.next^.v:1)
end.
