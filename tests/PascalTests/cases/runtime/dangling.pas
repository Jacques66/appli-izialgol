program dangling(output);
var p, q: ^integer;
begin
   new(p); p^ := 5; q := p; dispose(p);
   writeln(q^:1)
end.
