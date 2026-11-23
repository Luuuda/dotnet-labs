/* =====================================================================
   Проєкт №3 — cinema_reviews
   03_queries.js — запити перевірки: гнучка схема, CRUD, пагінація,
   embedding / referencing / гібрид, валідація, aggregation pipeline.
   Тестові зміни в кінці прибираються — скрипт можна запускати повторно.
   ===================================================================== */
db = db.getSiblingDB("cinema_reviews");
const I = (n) => NumberInt(n);
const show = (title, v) => { print(`\n===== ${title} =====`); printjson(v); };

/* 1. КРИТЕРІЙ: db.reviews.find() повертає документи з РІЗНИМИ наборами полів */
show("1. Гнучка схема: набір полів кожного відгуку",
  db.reviews.find().toArray().map(d => ({ _id: d._id, fields: Object.keys(d).join(", ") })));

/* 2. Read + пагінація: відгуки фільму, нові зверху (індекс ix_movie_createdAt) */
const page = 1, pageSize = 2;
show("2. Відгуки «Дюни», сторінка 1 по 2",
  db.reviews.find({ movieId: I(3) }, { movieTitle: 1, "author.displayName": 1, rating: 1, createdAt: 1 })
    .sort({ createdAt: -1 }).skip((page - 1) * pageSize).limit(pageSize).toArray());

/* 3. EMBEDDING: коментарі читаються разом із відгуком одним запитом */
show("3. Embedding: відгук разом із вбудованими коментарями",
  db.reviews.findOne({ _id: ObjectId("000000000000000000000301") }, { text: 1, rating: 1, comments: 1 }));

/* 4. REFERENCING + ГІБРИД: тема містить 3 останні пости (вбудовано),
      а повний список — у discussionPosts за посиланням discussionId */
const disc = db.discussions.findOne({ _id: ObjectId("000000000000000000000101") });
show("4a. Гібрид: тема з вбудованими lastPosts", { title: disc.title, postsCount: disc.postsCount, lastPosts: disc.lastPosts });
show("4b. Referencing: усі пости теми (ix_discussion_createdAt), сторінка 1",
  db.discussionPosts.find({ discussionId: disc._id }, { "author.displayName": 1, text: 1, createdAt: 1 })
    .sort({ createdAt: 1 }).limit(10).toArray());

/* 5. $lookup: відгук + тема обговорення, на яку він посилається */
show("5. $lookup reviews -> discussions",
  db.reviews.aggregate([
    { $match: { discussionId: { $exists: true } } },
    { $lookup: { from: "discussions", localField: "discussionId", foreignField: "_id", as: "discussion" } },
    { $project: { movieTitle: 1, rating: 1, "discussion.title": 1, "discussion.status": 1 } }
  ]).toArray());

/* 6. Пошук */
show("6a. Повнотекстовий пошук «IMAX»",
  db.reviews.find({ $text: { $search: "IMAX" } }, { movieTitle: 1, text: 1 }).toArray());
show("6b. Відгуки перевірених глядачів з оцінкою 5",
  db.reviews.find({ verifiedViewer: true, rating: 5 }, { movieTitle: 1, "author.displayName": 1 }).toArray());
show("6c. Теми з опитуванням (поле є лише в частини документів)",
  db.discussions.find({ poll: { $exists: true } }, { title: 1, "poll.question": 1 }).toArray());

/* 7. Валідація JSON Schema: rating = 7 має бути відхилено */
print("\n===== 7. JSON Schema Validation =====");
try {
  db.reviews.insertOne({ movieId: I(5), movieTitle: "Головоломка 2",
    author: { userId: "6f1c2a10-0000-4000-8000-000000000004", displayName: "Тарас Мельник" },
    rating: I(7), createdAt: new Date() });
  print("FAIL: документ з rating = 7 вставлено");
} catch (e) {
  print(`OK: відхилено валідатором: ${e.code} ${(e.errmsg || e.message || "").split("\n")[0]}`);
}
try {
  db.reviews.insertOne({ movieId: I(5), movieTitle: "Головоломка 2", rating: I(4), createdAt: new Date() });
  print("FAIL: документ без author вставлено");
} catch (e) {
  print(`OK: без обов'язкового author: ${e.code} ${(e.errmsg || e.message || "").split("\n")[0]}`);
}

/* 8. Create / Update / Delete */
print("\n===== 8. CRUD =====");
const testReview = db.reviews.insertOne({
  movieId: I(5), movieTitle: "Головоломка 2",
  author: { userId: "6f1c2a10-0000-4000-8000-000000000004", displayName: "Тарас Мельник" },
  rating: I(5), text: "Тестовий відгук", comments: [], commentsCount: I(0), createdAt: new Date() });
print(`OK: Create: ${testReview.insertedId}`);

// додати коментар у вбудований масив; $slice тримає масив обмеженим (останні 50)
const upd = db.reviews.updateOne({ _id: testReview.insertedId }, {
  $push: { comments: { $each: [{ _id: new ObjectId(),
            author: { userId: "6f1c2a10-0000-4000-8000-000000000003", displayName: "Марія Бондар" },
            text: "Дякую за відгук!", createdAt: new Date() }], $slice: -50 } },
  $inc: { commentsCount: I(1) }, $currentDate: { updatedAt: true } });
print(`OK: Update ($push коментаря): modified = ${upd.modifiedCount}`);

// гібрид: новий пост = insert у discussionPosts + оновлення лічильника й lastPosts у темі
const post = { _id: new ObjectId(), discussionId: disc._id,
  author: { userId: "6f1c2a10-0000-4000-8000-000000000003", displayName: "Марія Бондар" },
  text: "Тестовий пост", createdAt: new Date() };
db.discussionPosts.insertOne(post);
db.discussions.updateOne({ _id: disc._id }, {
  $push: { lastPosts: { $each: [{ postId: post._id, author: post.author.displayName, text: post.text, createdAt: post.createdAt }], $slice: -3 } },
  $inc: { postsCount: I(1) }, $set: { lastActivityAt: post.createdAt } });
const after = db.discussions.findOne({ _id: disc._id });
print(`OK: Гібрид: postsCount ${disc.postsCount} -> ${after.postsCount}, lastPosts = ${after.lastPosts.length} (не більше 3)`);

/* 9. Політика дублювання: обробник події MovieRenamed з Каталогу оновлює копії movieTitle */
print("\n===== 9. Подія MovieRenamed (movieId = 4) =====");
function onMovieRenamed(movieId, newTitle) {
  return ["reviews", "discussions"].map(c => `${c}: ${db[c].updateMany({ movieId: I(movieId) }, { $set: { movieTitle: newTitle } }).modifiedCount}`)
    .concat(`movieRatings: ${db.movieRatings.updateOne({ _id: I(movieId) }, { $set: { movieTitle: newTitle } }).modifiedCount}`);
}
printjson(onMovieRenamed(4, "Мавка. Лісова пісня (режисерська версія)"));
printjson(onMovieRenamed(4, "Мавка. Лісова пісня"));   // повертаємо назву

/* 10. Aggregation pipeline */
show("10a. Рейтинг фільмів (movieRatings, ix_average)",
  db.movieRatings.find({}, { movieTitle: 1, average: 1, count: 1, distribution: 1 }).sort({ average: -1, count: -1 }).toArray());
show("10b. Середні оцінки за аспектами (лише відгуки, де aspects є)",
  db.reviews.aggregate([
    { $match: { aspects: { $exists: true } } },
    { $project: { movieTitle: 1, a: { $objectToArray: "$aspects" } } },
    { $unwind: "$a" },
    { $group: { _id: { movie: "$movieTitle", aspect: "$a.k" }, avg: { $avg: "$a.v" } } },
    { $sort: { "_id.movie": 1, "_id.aspect": 1 } }
  ]).toArray());
show("10c. Найактивніші автори (відгуки + коментарі + пости)",
  db.reviews.aggregate([
    { $project: { items: { $concatArrays: [[{ name: "$author.displayName", kind: "review" }],
                   { $map: { input: { $ifNull: ["$comments", []] }, in: { name: "$$this.author.displayName", kind: "comment" } } }] } } },
    { $unwind: "$items" },
    { $replaceWith: "$items" },
    { $unionWith: { coll: "discussionPosts", pipeline: [{ $project: { _id: 0, name: "$author.displayName", kind: "post" } }] } },
    { $group: { _id: "$name", total: { $sum: 1 }, kinds: { $push: "$kind" } } },
    { $sort: { total: -1 } }
  ]).toArray());

/* 11. Прибирання тестових змін */
db.reviews.deleteOne({ _id: testReview.insertedId });
db.discussionPosts.deleteOne({ _id: post._id });
db.discussions.updateOne({ _id: disc._id },
  { $set: { postsCount: I(disc.postsCount), lastPosts: disc.lastPosts, lastActivityAt: disc.lastActivityAt } });
print(`\nOK: Delete: тестові документи видалено, reviews = ${db.reviews.countDocuments()}, discussionPosts = ${db.discussionPosts.countDocuments()}`);
