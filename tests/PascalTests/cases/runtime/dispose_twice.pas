program disposetwice(output);
var p, q: ^integer;
begin
   new(p); q := p; dispose(p); dispose(q)
end.
