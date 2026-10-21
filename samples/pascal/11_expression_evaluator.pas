(* Recursive descent evaluator of arithmetic expressions read from the input,
  one per line:
     expression = term [("+" | "-") term]...
     term       = factor [("*" | "/") factor]...
     factor     = number | "(" expression ")" | "-" factor
  ([x]... means: x repeated zero or more times; a closing brace would end
  this comment, the two kinds of comment brackets being synonyms.)
  Errors are reported and the rest of the line is skipped with a non-local
  goto out of the recursive functions. *)
program calculator(input, output);
label 9;
var ch: char;
    value: real;
    errors: integer;

procedure nextch;
begin
   if eoln then ch := ';' else read(ch)
end;

procedure skipblanks;
begin
   while ch = ' ' do nextch
end;

procedure error(msg: alfa);
begin
   writeln('  error: ', msg);
   errors := errors + 1;
   goto 9
end;

function expression: real; forward;

function factor: real;
var v: real;
begin
   skipblanks;
   if ch in ['0'..'9'] then
   begin
      v := 0;
      while ch in ['0'..'9'] do
      begin v := 10 * v + (ord(ch) - ord('0')); nextch end;
      factor := v
   end
   else if ch = '(' then
   begin
      nextch;
      v := expression;
      skipblanks;
      if ch <> ')' then error(') missing ');
      nextch;
      factor := v
   end
   else if ch = '-' then
   begin
      nextch; factor := -factor
   end
   else error('bad factor')
end;

function term: real;
var v, w: real;
    op: char;
begin
   v := factor;
   skipblanks;
   while ch in ['*', '/'] do
   begin
      op := ch; nextch;
      w := factor;
      if op = '*' then v := v * w
      else if w = 0 then error('div by 0  ')
      else v := v / w;
      skipblanks
   end;
   term := v
end;

function expression;
var v: real;
    op: char;
begin
   v := term;
   while ch in ['+', '-'] do
   begin
      op := ch; nextch;
      if op = '+' then v := v + term else v := v - term
   end;
   expression := v
end;

begin
   errors := 0;
   while not eof do
   begin
      nextch;
      value := expression;
      if ch <> ';' then error('extra text')
      else writeln('  = ', value:1:4);
9:    readln
   end;
   writeln(errors:1, ' error(s)')
end.
