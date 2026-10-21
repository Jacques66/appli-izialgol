program eolneof(output);
var f: text;
begin
   rewrite(f); writeln(f, 'x'); reset(f); readln(f);
   writeln(eof(f));
   writeln(eoln(f))
end.
