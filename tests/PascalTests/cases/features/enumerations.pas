program enums(output);
type day = (mon, tue, wed, thu, fri, sat, sun);
     weekend = sat..sun;
var d: day; w: weekend; count: integer;
begin
   count := 0;
   for d := mon to sun do count := count + ord(d);
   writeln(count:1, ' ', ord(sun):1, ' ', ord(succ(mon)):1, ' ', ord(pred(sun)):1);
   w := sat; d := w; writeln(d = sat, d < sun, mon > tue, ord(w):2);
   for d := sun downto fri do
      case d of
         mon, tue, wed, thu, fri: writeln('work');
         sat, sun: writeln('rest')
      end;
   writeln(false < true, ord(false):2, ord(true):2, succ(false))
end.
