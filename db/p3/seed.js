/* =====================================================================
   Проєкт №3 — cinema_reviews
   02_seed.js — тестові документи. Ідемпотентний (find-or-create):
   updateOne({_id}, {$setOnInsert: doc}, {upsert: true}) — повторний
   запуск не створює дублікатів і не перезаписує змінені документи.
   userId = Customers.Id з CinemaBooking, movieId = Movies.Id з CinemaCatalog.
   ===================================================================== */
db = db.getSiblingDB("cinema_reviews");

const I = (n) => NumberInt(n);
const L = (n) => NumberLong(String(n));
const D = (s) => ISODate(s);
const id = (n) => ObjectId(n.toString(16).padStart(24, "0"));   // фіксовані ObjectId

const users = {
  olena:  { userId: "6f1c2a10-0000-4000-8000-000000000001", displayName: "Олена Коваленко" },
  andrii: { userId: "6f1c2a10-0000-4000-8000-000000000002", displayName: "Андрій Шевчук" },
  maria:  { userId: "6f1c2a10-0000-4000-8000-000000000003", displayName: "Марія Бондар" },
  taras:  { userId: "6f1c2a10-0000-4000-8000-000000000004", displayName: "Тарас Мельник" }
};

function upsertAll(collection, docs) {
  let inserted = 0;
  for (const doc of docs) {
    const { _id, ...rest } = doc;
    const r = db[collection].updateOne({ _id }, { $setOnInsert: rest }, { upsert: true });
    inserted += r.upsertedCount;
  }
  print(`${collection}: нових ${inserted}, всього ${db[collection].countDocuments()}`);
}

/* ---------------- discussionPosts (REFERENCING: необмежений ріст) ---------------- */
const posts = [
  // тема 0x101 — Дюна
  { _id: id(0x201), discussionId: id(0x101), author: users.andrii, createdAt: D("2026-11-15T18:00:00Z"),
    text: "Як думаєте, третя частина буде за «Месією Дюни» буквально чи з великими змінами?",
    reactions: { like: I(4) } },
  { _id: id(0x202), discussionId: id(0x101), author: users.olena, createdAt: D("2026-11-15T19:10:00Z"),
    text: "Вільнев казав, що це буде окремий, камерніший фільм.", reactions: { like: I(6), fire: I(1) } },
  { _id: id(0x203), discussionId: id(0x101), author: users.maria, createdAt: D("2026-11-16T08:30:00Z"),
    text: "Головне, щоб Ганс Циммер знову писав музику.", replyToPostId: id(0x202) },
  { _id: id(0x204), discussionId: id(0x101), author: users.taras, createdAt: D("2026-11-17T21:45:00Z"),
    text: "Перечитую книгу, щоб бути готовим :)", editedAt: D("2026-11-17T21:50:00Z") },
  // тема 0x102 — Мавка
  { _id: id(0x205), discussionId: id(0x102), author: users.maria, createdAt: D("2026-11-10T09:00:00Z"),
    text: "Дітям 6 і 9 років — нормально буде?" },
  { _id: id(0x206), discussionId: id(0x102), author: users.olena, createdAt: D("2026-11-10T10:15:00Z"),
    text: "Так, ходили з малими, всім сподобалось.", reactions: { like: I(2) } },
  // тема 0x103 — Довбуш
  { _id: id(0x207), discussionId: id(0x103), author: users.taras, createdAt: D("2026-11-05T20:00:00Z"),
    text: "Наскільки фільм відповідає історичним джерелам про опришків?" }
];

const snippet = (p) => ({ postId: p._id, author: p.author.displayName,
                          text: p.text.slice(0, 80), createdAt: p.createdAt });

/* ---------------- discussions (ГІБРИД: lastPosts вбудовані + посилання на discussionPosts) -------- */
const discussions = [
  { _id: id(0x101), movieId: I(3), movieTitle: "Дюна: Частина друга",
    title: "Теорії про третю частину", createdBy: users.andrii, status: "open",
    isPinned: true, tags: ["теорії", "книга", "продовження"],
    postsCount: I(4), lastPosts: posts.slice(1, 4).map(snippet),
    createdAt: D("2026-11-15T18:00:00Z"), lastActivityAt: D("2026-11-17T21:45:00Z") },

  // гнучка схема: тема-опитування має поле poll, якого немає в інших
  { _id: id(0x102), movieId: I(4), movieTitle: "Мавка. Лісова пісня",
    title: "Куди піти з дітьми на «Мавку»?", createdBy: users.maria, status: "open",
    poll: { question: "З якого віку дивитися?",
            options: [ { text: "з 4 років", votes: I(3) }, { text: "з 6 років", votes: I(7) },
                       { text: "з 10 років", votes: I(1) } ],
            closesAt: D("2026-12-31T00:00:00Z") },
    postsCount: I(2), lastPosts: posts.slice(4, 6).map(snippet),
    createdAt: D("2026-11-10T09:00:00Z"), lastActivityAt: D("2026-11-10T10:15:00Z") },

  // закрита тема — має closedAt / closedReason
  { _id: id(0x103), movieId: I(1), movieTitle: "Довбуш",
    title: "Історична достовірність фільму", createdBy: users.taras, status: "closed",
    closedAt: D("2026-11-12T00:00:00Z"), closedReason: "Тему перенесено до розділу «Історія»",
    postsCount: I(1), lastPosts: posts.slice(6, 7).map(snippet),
    createdAt: D("2026-11-05T20:00:00Z"), lastActivityAt: D("2026-11-05T20:00:00Z") }
];

/* ---------------- reviews (EMBEDDING коментарів + різні набори полів) ---------------- */
const reviews = [
  // повний відгук: аспекти, фото, вбудовані коментарі, посилання на обговорення, «перевірений глядач»
  { _id: id(0x301), movieId: I(3), movieTitle: "Дюна: Частина друга", author: users.olena,
    rating: I(5), text: "Найкраще, що я бачила в IMAX. Масштаб, звук, кожен кадр — як картина.",
    verifiedViewer: true, bookingId: L(1), watchedFormat: "IMAX",
    aspects: { story: I(5), acting: I(5), visuals: I(5), sound: I(5) },
    photos: ["reviews/301/hall.jpg", "reviews/301/ticket.jpg"],
    comments: [
      { _id: id(0x401), author: users.andrii, text: "Згоден, IMAX того вартий!", createdAt: D("2026-11-21T09:00:00Z") },
      { _id: id(0x402), author: users.maria,  text: "Звук справді неймовірний.",  createdAt: D("2026-11-21T09:30:00Z") }
    ],
    commentsCount: I(2), discussionId: id(0x101),
    createdAt: D("2026-11-20T22:00:00Z") },

  // відгук зі спойлером — поле spoiler є лише тут
  { _id: id(0x302), movieId: I(3), movieTitle: "Дюна: Частина друга", author: users.andrii,
    rating: I(4), text: "Фінал трохи поспішний, але дуель — топ.", spoiler: true,
    comments: [], commentsCount: I(0), createdAt: D("2026-11-18T20:00:00Z") },

  // мінімальний відгук: лише обов'язкові поля + теги
  { _id: id(0x303), movieId: I(3), movieTitle: "Дюна: Частина друга", author: users.maria,
    rating: I(4), tags: ["саундтрек", "візуал"], createdAt: D("2026-11-19T12:00:00Z") },

  { _id: id(0x304), movieId: I(1), movieTitle: "Довбуш", author: users.olena,
    rating: I(5), text: "Гарні Карпати і сильна головна роль.",
    aspects: { story: I(4), acting: I(5) },
    comments: [ { _id: id(0x403), author: users.taras, text: "Костюми теж вражають.", createdAt: D("2026-11-06T08:00:00Z") } ],
    commentsCount: I(1), discussionId: id(0x103), createdAt: D("2026-11-05T21:00:00Z") },

  // інша структура: плюси/мінуси списками
  { _id: id(0x305), movieId: I(1), movieTitle: "Довбуш", author: users.taras,
    rating: I(4), pros: ["атмосфера", "музика"], cons: ["затягнутий початок"],
    createdAt: D("2026-11-06T19:00:00Z") },

  { _id: id(0x306), movieId: I(2), movieTitle: "Оппенгеймер", author: users.andrii,
    rating: I(5), text: "Три години пролетіли непомітно.", verifiedViewer: true, bookingId: L(2),
    watchedFormat: "VIP", createdAt: D("2026-11-20T23:30:00Z") },

  // сімейний відгук: вкладений об'єкт про перегляд з дітьми
  { _id: id(0x307), movieId: I(4), movieTitle: "Мавка. Лісова пісня", author: users.maria,
    rating: I(4), text: "Діти в захваті, дорослим теж цікаво.",
    watchedWith: { type: "family", kidsAges: [I(6), I(9)] },
    discussionId: id(0x102), createdAt: D("2026-11-11T15:00:00Z") }
];

upsertAll("discussionPosts", posts);
upsertAll("discussions", discussions);
upsertAll("reviews", reviews);

/* ---------------- movieRatings — будується aggregation pipeline з reviews ----------------
   $merge повністю перераховує агрегат, тому повторний запуск теж не дублює. */
db.reviews.aggregate([
  { $group: { _id: "$movieId", movieTitle: { $first: "$movieTitle" },
              average: { $avg: "$rating" }, count: { $sum: 1 }, ratings: { $push: "$rating" } } },
  { $project: {
      movieTitle: 1, count: 1, average: { $round: ["$average", 2] },
      distribution: { $arrayToObject: { $map: {
          input: [1, 2, 3, 4, 5], as: "r",
          in: { k: { $toString: "$$r" },
                v: { $size: { $filter: { input: "$ratings", cond: { $eq: ["$$this", "$$r"] } } } } } } } },
      updatedAt: "$$NOW" } },
  { $merge: { into: "movieRatings", on: "_id", whenMatched: "replace", whenNotMatched: "insert" } }
]);
print(`movieRatings: ${db.movieRatings.countDocuments()} фільми з рейтингом`);
