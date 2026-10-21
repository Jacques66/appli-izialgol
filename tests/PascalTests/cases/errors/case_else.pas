program caseelse(output);
var i: integer;
begin
   i := 2;
   case i of
      1: writeln('one');
      2, 1: writeln('two');
      'c': writeln('char');
   else writeln('other')
   end
end.
