program casenomatch(output);
type color = (red, green, blue);
var c: color;
begin
   for c := red to blue do
      case c of
         red: writeln('red');
         green: writeln('green')
      end
end.
