program comments(output);
{ The two comment brackets are synonyms (User Manual and Report, chapter 3):
  either closing symbol ends a comment, and comments do not nest. }
var i: integer;
begin
   i := 1; (* opened with a parenthesis, closed with a brace }
   i := i + 10; { closed by a star and a parenthesis *) i := i + 100;
   writeln(i)  { 111 }
end.
